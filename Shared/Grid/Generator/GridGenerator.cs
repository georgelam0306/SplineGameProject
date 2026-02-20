#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Grid.Generator;

[Generator]
public sealed class GridGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Detect [WorldGrid] assembly-level attribute
        var worldGridProvider = context.CompilationProvider.Select((compilation, _) =>
        {
            foreach (var attr in compilation.Assembly.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::Grid.WorldGridAttribute")
                {
                    int tileSize = 32;
                    int widthTiles = 256;
                    int heightTiles = 256;
                    string ns = "Grid.Generated";

                    foreach (var kvp in attr.NamedArguments)
                    {
                        switch (kvp.Key)
                        {
                            case "TileSize":
                                tileSize = (int)kvp.Value.Value!;
                                break;
                            case "WidthTiles":
                                widthTiles = (int)kvp.Value.Value!;
                                break;
                            case "HeightTiles":
                                heightTiles = (int)kvp.Value.Value!;
                                break;
                            case "Namespace":
                                ns = (string)kvp.Value.Value!;
                                break;
                        }
                    }

                    return new WorldGridModel(tileSize, widthTiles, heightTiles, ns);
                }
            }
            return null;
        });

        // Detect [DataGrid] struct-level attributes
        var dataGridProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
            fullyQualifiedMetadataName: "Grid.DataGridAttribute",
            predicate: static (node, _) => node is StructDeclarationSyntax,
            transform: static (ctx, _) => TransformDataGrid(ctx))
            .Where(static model => model is not null);

        // Combine world grid with all data grids
        var combined = worldGridProvider.Combine(dataGridProvider.Collect());

        // Generate source
        context.RegisterSourceOutput(combined, static (spc, data) =>
        {
            var (worldGrid, dataGrids) = data;

            if (worldGrid == null)
            {
                return;
            }

            // Generate WorldTile
            var worldTileSource = WorldGridRenderer.Render(worldGrid, dataGrids);
            spc.AddSource("WorldTile.g.cs", SourceText.From(worldTileSource, Encoding.UTF8));

            // Generate each DataGrid type
            if (!dataGrids.IsDefaultOrEmpty)
            {
                foreach (var dataGrid in dataGrids.Where(d => d != null))
                {
                    var source = DataGridRenderer.Render(dataGrid!, worldGrid, dataGrids);
                    spc.AddSource($"{dataGrid!.TypeName}.g.cs", SourceText.From(source, Encoding.UTF8));
                }
            }
        });
    }

    private static DataGridModel? TransformDataGrid(GeneratorAttributeSyntaxContext ctx)
    {
        var structDecl = (StructDeclarationSyntax)ctx.TargetNode;
        var typeSymbol = (INamedTypeSymbol)ctx.TargetSymbol;

        // Must be partial
        if (!typeSymbol.DeclaringSyntaxReferences.Any(s =>
            s.GetSyntax() is StructDeclarationSyntax sd &&
            sd.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword))))
        {
            return null;
        }

        // No generic types
        if (typeSymbol.IsGenericType || (typeSymbol.ContainingType != null && typeSymbol.ContainingType.IsGenericType))
        {
            return null;
        }

        int cellSizePixels = 0;
        int dimensions = 0;

        foreach (var attr in typeSymbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::Grid.DataGridAttribute")
            {
                foreach (var kvp in attr.NamedArguments)
                {
                    switch (kvp.Key)
                    {
                        case "CellSizePixels":
                            cellSizePixels = (int)kvp.Value.Value!;
                            break;
                        case "Dimensions":
                            dimensions = (int)kvp.Value.Value!;
                            break;
                    }
                }
            }
        }

        if (cellSizePixels <= 0 || dimensions <= 0)
        {
            return null;
        }

        var ns = typeSymbol.ContainingNamespace.ToDisplayString();
        return new DataGridModel(typeSymbol.Name, cellSizePixels, dimensions, ns, typeSymbol);
    }
}
