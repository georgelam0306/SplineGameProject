// SPDX-License-Identifier: MIT
#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace Property.Generator
{
    internal static class TypeHelpers
    {
        private const string PooledAttributeName = "global::Pooled.PooledAttribute";
        private const string ColumnAttributeName = "global::Pooled.ColumnAttribute";

        public static bool TryGetPooledSettings(INamedTypeSymbol typeSymbol, out PooledSettings settings)
        {
            settings = new PooledSettings(soA: true, poolId: 0);
            foreach (var attribute in typeSymbol.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == PooledAttributeName)
                {
                    bool soA = true;
                    ushort poolId = 0;
                    foreach (var namedArg in attribute.NamedArguments)
                    {
                        switch (namedArg.Key)
                        {
                            case "SoA":
                                if (namedArg.Value.Value is bool soAValue)
                                {
                                    soA = soAValue;
                                }
                                break;
                            case "PoolId":
                                if (namedArg.Value.Value is ushort poolIdValue)
                                {
                                    poolId = poolIdValue;
                                }
                                else if (namedArg.Value.Value is int poolIdInt)
                                {
                                    poolId = (ushort)poolIdInt;
                                }
                                break;
                        }
                    }
                    settings = new PooledSettings(soA, poolId);
                    return true;
                }
            }
            return false;
        }

        public static bool IsPartialStruct(INamedTypeSymbol typeSymbol)
        {
            if (typeSymbol.TypeKind != TypeKind.Struct)
            {
                return false;
            }

            foreach (var syntaxRef in typeSymbol.DeclaringSyntaxReferences)
            {
                if (syntaxRef.GetSyntax() is StructDeclarationSyntax structDecl)
                {
                    if (structDecl.Modifiers.Any(mod => mod.IsKind(SyntaxKind.PartialKeyword)))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static bool IsPublicOrInternal(INamedTypeSymbol typeSymbol)
        {
            return typeSymbol.DeclaredAccessibility == Accessibility.Public ||
                   typeSymbol.DeclaredAccessibility == Accessibility.Internal;
        }

        public static bool TryGetColumnReadOnly(IFieldSymbol fieldSymbol, out bool isReadOnly)
        {
            isReadOnly = false;
            foreach (var attribute in fieldSymbol.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == ColumnAttributeName)
                {
                    foreach (var namedArg in attribute.NamedArguments)
                    {
                        if (namedArg.Key == "ReadOnly" && namedArg.Value.Value is bool readOnlyValue)
                        {
                            isReadOnly = readOnlyValue;
                        }
                    }
                    return true;
                }
            }
            return false;
        }
    }
}
