// SPDX-License-Identifier: MIT
#nullable enable
using DerpLib.DI.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace DerpLib.DI.Generator.Parser
{
    /// <summary>
    /// Parses the DI.Setup() fluent API chain.
    /// </summary>
    internal static class SetupParser
    {
        /// <summary>
        /// Parses the Setup() method body and extracts all configuration.
        /// </summary>
        public static void Parse(
            MethodDeclarationSyntax setupMethod,
            SemanticModel semanticModel,
            CompositionModel model)
        {
            // Find the DI.Setup() chain
            var chain = GetFluentChain(setupMethod);
            if (chain.Count == 0)
            {
                model.Diagnostics.Add(Diagnostic.Create(
                    Diagnostics.MissingSetup,
                    setupMethod.GetLocation(),
                    model.ClassName));
                return;
            }

            // Parse each call in the chain
            ITypeSymbol? currentBindingType = null;
            string? currentBindingTag = null;
            BindingLifetime currentLifetime = BindingLifetime.Singleton;
            bool hasLifetime = false;

            // BindAll state
            CollectionBindingModel? currentCollection = null;

            foreach (var invocation in chain)
            {
                var methodName = GetMethodName(invocation);

                // Before processing a new top-level call, flush any pending self-binding
                // A self-binding is pending when we have Bind + As but the next call isn't To
                if (currentBindingType is not null && hasLifetime && methodName != "To")
                {
                    // Self-binding: .Bind<T>().As(Singleton) without .To<>()
                    var selfBinding = new BindingModel(
                        currentBindingType, currentBindingTag,
                        currentBindingType, null, currentLifetime);
                    model.Bindings.Add(selfBinding);
                    currentBindingType = null;
                    currentBindingTag = null;
                    currentLifetime = BindingLifetime.Singleton;
                    hasLifetime = false;
                }

                // Flush any pending collection when we hit a call that isn't part of the chain
                // BindAll → As → Add+ is the full chain, so only flush on other calls
                if (currentCollection is not null && methodName != "Add" && methodName != "As")
                {
                    model.CollectionBindings.Add(currentCollection);
                    currentCollection = null;
                }

                switch (methodName)
                {
                    case "Setup":
                        break;

                    case "Arg":
                        ParseArg(invocation, semanticModel, model);
                        break;

                    case "Bind":
                        (currentBindingType, currentBindingTag) = ParseBindStart(invocation, semanticModel);
                        hasLifetime = false;
                        break;

                    case "BindAll":
                    {
                        var typeArg = GetTypeArgument(invocation, semanticModel, 0);
                        if (typeArg is not null)
                        {
                            // Collection starts — lifetime will be set by As()
                            currentCollection = new CollectionBindingModel(typeArg, BindingLifetime.Singleton);
                        }
                        break;
                    }

                    case "As":
                        if (currentCollection is not null)
                        {
                            currentCollection = new CollectionBindingModel(
                                currentCollection.ServiceType, ParseLifetime(invocation));
                        }
                        else
                        {
                            currentLifetime = ParseLifetime(invocation);
                            hasLifetime = true;
                        }
                        break;

                    case "Add":
                    {
                        if (currentCollection is not null)
                        {
                            var typeArg = GetTypeArgument(invocation, semanticModel, 0);
                            if (typeArg is not null)
                            {
                                currentCollection.Items.Add(new CollectionItem(typeArg));
                            }
                        }
                        break;
                    }

                    case "To":
                        if (currentBindingType is not null)
                        {
                            var binding = ParseBindTo(invocation, semanticModel, currentBindingType, currentBindingTag, currentLifetime, model);
                            if (binding is not null)
                            {
                                model.Bindings.Add(binding);
                            }
                        }
                        currentBindingType = null;
                        currentBindingTag = null;
                        currentLifetime = BindingLifetime.Singleton;
                        hasLifetime = false;
                        break;

                    case "Scope":
                        ParseScope(invocation, semanticModel, model);
                        break;

                    case "Root":
                        ParseRoot(invocation, semanticModel, model);
                        break;
                }
            }

            // Flush any trailing self-binding
            if (currentBindingType is not null && hasLifetime)
            {
                var selfBinding = new BindingModel(
                    currentBindingType, currentBindingTag,
                    currentBindingType, null, currentLifetime);
                model.Bindings.Add(selfBinding);
            }

            // Flush any trailing collection
            if (currentCollection is not null)
            {
                model.CollectionBindings.Add(currentCollection);
            }
        }

        private static List<InvocationExpressionSyntax> GetFluentChain(MethodDeclarationSyntax method)
        {
            var result = new List<InvocationExpressionSyntax>();

            // Find the expression body or arrow expression
            ExpressionSyntax? expr = method.ExpressionBody?.Expression;
            if (expr is null && method.Body is not null)
            {
                // Look for return statement or expression statement
                var returnStmt = method.Body.Statements.OfType<ReturnStatementSyntax>().FirstOrDefault();
                if (returnStmt?.Expression is not null)
                {
                    expr = returnStmt.Expression;
                }
                else
                {
                    var exprStmt = method.Body.Statements.OfType<ExpressionStatementSyntax>().FirstOrDefault();
                    expr = exprStmt?.Expression;
                }
            }

            // Walk the invocation chain from inside out
            while (expr is InvocationExpressionSyntax invocation)
            {
                result.Insert(0, invocation);

                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    expr = memberAccess.Expression;
                }
                else
                {
                    break;
                }
            }

            return result;
        }

        private static string? GetMethodName(InvocationExpressionSyntax invocation)
        {
            return invocation.Expression switch
            {
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
                IdentifierNameSyntax identifier => identifier.Identifier.Text,
                _ => null
            };
        }

        private static void ParseArg(InvocationExpressionSyntax invocation, SemanticModel semanticModel, CompositionModel model)
        {
            // .Arg<T>("name")
            var typeArg = GetTypeArgument(invocation, semanticModel, 0);
            var nameArg = GetStringArgument(invocation, 0);

            if (typeArg is not null && nameArg is not null)
            {
                model.Args.Add(new ArgModel(typeArg, nameArg));
            }
        }

        private static (ITypeSymbol? type, string? tag) ParseBindStart(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
        {
            // .Bind<T>() or .Bind<T>("tag")
            var typeArg = GetTypeArgument(invocation, semanticModel, 0);
            var tagArg = GetStringArgument(invocation, 0); // Tag is optional first string arg

            return (typeArg, tagArg);
        }

        private static BindingLifetime ParseLifetime(InvocationExpressionSyntax invocation)
        {
            // .As(Singleton) or .As(Transient)
            if (invocation.ArgumentList.Arguments.Count > 0)
            {
                var arg = invocation.ArgumentList.Arguments[0].Expression;
                var text = arg.ToString();
                if (text.Contains("Transient"))
                    return BindingLifetime.Transient;
            }
            return BindingLifetime.Singleton;
        }

        private static BindingModel? ParseBindTo(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            ITypeSymbol serviceType,
            string? tag,
            BindingLifetime lifetime,
            CompositionModel model)
        {
            // .To<TImpl>() or .To(ctx => ...)
            var typeArg = GetTypeArgument(invocation, semanticModel, 0);
            LambdaExpressionSyntax? factory = null;

            if (typeArg is not null)
            {
                // Type binding
                return new BindingModel(serviceType, tag, typeArg, null, lifetime);
            }

            // Check for factory lambda
            if (invocation.ArgumentList.Arguments.Count > 0)
            {
                var arg = invocation.ArgumentList.Arguments[0].Expression;
                if (arg is LambdaExpressionSyntax lambda)
                {
                    factory = lambda;
                }
            }

            if (factory is not null)
            {
                var binding = new BindingModel(serviceType, tag, null, factory, lifetime);
                PopulateFactoryInfo(binding, factory, semanticModel, model);
                return binding;
            }

            return null;
        }

        private static void PopulateFactoryInfo(
            BindingModel binding,
            LambdaExpressionSyntax lambda,
            SemanticModel semanticModel,
            CompositionModel model)
        {
            if (lambda.Body is ExpressionSyntax exprBody)
            {
                binding.FactoryIsBlock = false;
                binding.FactoryExpressionText = NormalizeFactoryExpression(exprBody, semanticModel);
                return;
            }

            if (lambda.Body is BlockSyntax blockBody)
            {
                binding.FactoryIsBlock = true;
                var contextParamName = GetFactoryContextParameterName(lambda);
                if (contextParamName is null)
                {
                    model.Diagnostics.Add(Diagnostic.Create(
                        Diagnostics.UnsupportedFactoryLambda,
                        lambda.GetLocation(),
                        binding.ServiceType.Name,
                        model.ClassName));
                    binding.FactoryExpressionText = "default!";
                    return;
                }

                ExpressionSyntax? returnExpr = null;

                for (int i = 0; i < blockBody.Statements.Count; i++)
                {
                    var statement = blockBody.Statements[i];

                    if (statement is ReturnStatementSyntax ret)
                    {
                        if (i != blockBody.Statements.Count - 1)
                        {
                            model.Diagnostics.Add(Diagnostic.Create(
                                Diagnostics.UnsupportedFactoryLambda,
                                statement.GetLocation(),
                                binding.ServiceType.Name,
                                model.ClassName));
                        }

                        returnExpr = ret.Expression;
                        break;
                    }

                    if (statement is ExpressionStatementSyntax exprStmt &&
                        exprStmt.Expression is InvocationExpressionSyntax invocation)
                    {
                        if (!TryParseFactoryInjection(invocation, semanticModel, contextParamName, out var injection))
                        {
                            model.Diagnostics.Add(Diagnostic.Create(
                                Diagnostics.UnsupportedFactoryLambda,
                                statement.GetLocation(),
                                binding.ServiceType.Name,
                                model.ClassName));
                            continue;
                        }

                        binding.FactoryInjections.Add(injection);
                        continue;
                    }

                    model.Diagnostics.Add(Diagnostic.Create(
                        Diagnostics.UnsupportedFactoryLambda,
                        statement.GetLocation(),
                        binding.ServiceType.Name,
                        model.ClassName));
                }

                if (returnExpr is null)
                {
                    model.Diagnostics.Add(Diagnostic.Create(
                        Diagnostics.UnsupportedFactoryLambda,
                        lambda.GetLocation(),
                        binding.ServiceType.Name,
                        model.ClassName));
                    binding.FactoryExpressionText = "default!";
                    return;
                }

                binding.FactoryExpressionText = NormalizeFactoryExpression(returnExpr, semanticModel);
                return;
            }

            model.Diagnostics.Add(Diagnostic.Create(
                Diagnostics.UnsupportedFactoryLambda,
                lambda.GetLocation(),
                binding.ServiceType.Name,
                model.ClassName));
            binding.FactoryExpressionText = "default!";
        }

        private static string? GetFactoryContextParameterName(LambdaExpressionSyntax lambda)
        {
            if (lambda is SimpleLambdaExpressionSyntax simple)
            {
                return simple.Parameter.Identifier.Text;
            }

            if (lambda is ParenthesizedLambdaExpressionSyntax parenthesized &&
                parenthesized.ParameterList.Parameters.Count == 1)
            {
                return parenthesized.ParameterList.Parameters[0].Identifier.Text;
            }

            return null;
        }

        private static bool TryParseFactoryInjection(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            string contextParamName,
            out FactoryInjectionModel injection)
        {
            injection = null!;

            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            if (memberAccess.Expression is not IdentifierNameSyntax receiver ||
                receiver.Identifier.Text != contextParamName)
            {
                return false;
            }

            var methodName = memberAccess.Name.Identifier.Text;

            var args = invocation.ArgumentList.Arguments;
            string? tag = null;
            ExpressionSyntax? outArgExpr = null;
            FactoryInjectionKind kind;

            switch (methodName)
            {
                case "Inject":
                    kind = FactoryInjectionKind.Inject;
                    if (args.Count == 1)
                    {
                        outArgExpr = args[0].Expression;
                    }
                    else if (args.Count == 2)
                    {
                        tag = GetStringArgument(invocation, 0);
                        outArgExpr = args[1].Expression;
                    }
                    else
                    {
                        return false;
                    }
                    break;

                case "InjectFromParent":
                    kind = FactoryInjectionKind.InjectFromParent;
                    if (args.Count != 1)
                    {
                        return false;
                    }
                    outArgExpr = args[0].Expression;
                    break;

                default:
                    return false;
            }

            if (outArgExpr is not DeclarationExpressionSyntax declExpr)
            {
                return false;
            }

            if (declExpr.Designation is not SingleVariableDesignationSyntax varDesig)
            {
                return false;
            }

            // Prefer the inferred type from the out declaration (supports `out T x` and `out var x`).
            // If the type is not inferable, fall back to the explicit generic type arg (supports `Inject<T>(out var x)`).
            ITypeSymbol? typeArg = semanticModel.GetTypeInfo(declExpr.Type).Type;
            if (typeArg is null || typeArg.TypeKind == TypeKind.Error)
            {
                if (memberAccess.Name is GenericNameSyntax genericName &&
                    genericName.TypeArgumentList.Arguments.Count == 1)
                {
                    var typeArgSyntax = genericName.TypeArgumentList.Arguments[0];
                    typeArg = semanticModel.GetTypeInfo(typeArgSyntax).Type;
                }
            }

            if (typeArg is null || typeArg.TypeKind == TypeKind.Error)
            {
                return false;
            }

            injection = new FactoryInjectionModel(
                kind,
                typeArg,
                tag,
                varDesig.Identifier.Text,
                invocation.GetLocation());
            return true;
        }

        private static string NormalizeFactoryExpression(ExpressionSyntax expression, SemanticModel semanticModel)
        {
            if (expression is ObjectCreationExpressionSyntax objectCreation)
            {
                var createdType = semanticModel.GetTypeInfo(objectCreation).Type;
                if (createdType is not null)
                {
                    var typeName = createdType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var args = objectCreation.ArgumentList?.ToString() ?? "()";
                    var initializer = objectCreation.Initializer?.ToString() ?? string.Empty;
                    return $"new {typeName}{args}{initializer}";
                }
            }

            var symbol = semanticModel.GetSymbolInfo(expression).Symbol;
            if (symbol is IPropertySymbol prop && prop.IsStatic)
            {
                return $"{prop.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{prop.Name}";
            }
            if (symbol is IFieldSymbol field && field.IsStatic)
            {
                return $"{field.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{field.Name}";
            }

            return expression.ToString();
        }

        private static void ParseScope(InvocationExpressionSyntax invocation, SemanticModel semanticModel, CompositionModel model)
        {
            // .Scope<TChild>()
            var typeArg = GetTypeArgument(invocation, semanticModel, 0);
            if (typeArg is INamedTypeSymbol named)
            {
                model.Scopes.Add(new ScopeModel(named));
            }
        }

        private static void ParseRoot(InvocationExpressionSyntax invocation, SemanticModel semanticModel, CompositionModel model)
        {
            // .Root<T>("name")
            var typeArg = GetTypeArgument(invocation, semanticModel, 0);
            var nameArg = GetStringArgument(invocation, 0);

            if (typeArg is not null && nameArg is not null)
            {
                model.Roots.Add(new RootModel(typeArg, nameArg));
            }
        }

        private static ITypeSymbol? GetTypeArgument(InvocationExpressionSyntax invocation, SemanticModel semanticModel, int index)
        {
            // Get type argument from generic method call
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name is GenericNameSyntax genericName &&
                genericName.TypeArgumentList.Arguments.Count > index)
            {
                var typeArg = genericName.TypeArgumentList.Arguments[index];
                return semanticModel.GetTypeInfo(typeArg).Type;
            }
            return null;
        }

        private static string? GetStringArgument(InvocationExpressionSyntax invocation, int index)
        {
            if (invocation.ArgumentList.Arguments.Count > index)
            {
                var arg = invocation.ArgumentList.Arguments[index].Expression;
                if (arg is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    return literal.Token.ValueText;
                }
            }
            return null;
        }
    }
}
