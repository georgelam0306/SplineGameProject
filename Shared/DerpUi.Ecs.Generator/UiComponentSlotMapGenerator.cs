using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DerpUi.Ecs.Generator;

[Generator]
public sealed class UiComponentSlotMapGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<ComponentInfo> componentInfos = context.SyntaxProvider.ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: "Pooled.PooledAttribute",
                predicate: static (node, _) => node is StructDeclarationSyntax,
                transform: static (syntaxContext, _) => TryGetComponentInfo(syntaxContext))
            .Where(static info => info.PoolId > 0);

        IncrementalValueProvider<ImmutableArray<ComponentInfo>> allComponentInfos = componentInfos.Collect();

        context.RegisterSourceOutput(allComponentInfos, static (sourceContext, infos) =>
        {
            EmitMap(sourceContext, infos);
            EmitNames(sourceContext, infos);
        });
    }

    private static ComponentInfo TryGetComponentInfo(GeneratorAttributeSyntaxContext syntaxContext)
    {
        int poolId = 0;
        foreach (AttributeData attributeData in syntaxContext.Attributes)
        {
            if (attributeData.AttributeClass == null)
            {
                continue;
            }

            if (attributeData.AttributeClass.ToDisplayString() != "Pooled.PooledAttribute")
            {
                continue;
            }

            foreach (KeyValuePair<string, TypedConstant> named in attributeData.NamedArguments)
            {
                if (named.Key == "PoolId" && named.Value.Kind == TypedConstantKind.Primitive)
                {
                    object? rawValue = named.Value.Value;
                    if (rawValue is byte byteValue)
                    {
                        poolId = byteValue;
                        break;
                    }
                    if (rawValue is short shortValue)
                    {
                        poolId = shortValue;
                        break;
                    }
                    if (rawValue is ushort ushortValue)
                    {
                        poolId = ushortValue;
                        break;
                    }
                    if (rawValue is int intValue)
                    {
                        poolId = intValue;
                        break;
                    }
                }
            }

            if (poolId > 0)
            {
                break;
            }
        }

        if (poolId <= 0)
        {
            return default;
        }

        string rawName = syntaxContext.TargetSymbol.Name;
        string displayName = rawName;
        const string suffix = "Component";
        if (rawName.EndsWith(suffix, StringComparison.Ordinal))
        {
            displayName = rawName.Substring(0, rawName.Length - suffix.Length);
        }

        return new ComponentInfo(poolId, displayName);
    }

    private static void EmitMap(SourceProductionContext context, ImmutableArray<ComponentInfo> infos)
    {
        if (infos.IsDefaultOrEmpty)
        {
            return;
        }

        var unique = new HashSet<int>();
        var ordered = new List<int>(capacity: infos.Length);
        for (int i = 0; i < infos.Length; i++)
        {
            int poolId = infos[i].PoolId;
            if (poolId <= 0)
            {
                continue;
            }
            if (unique.Add(poolId))
            {
                ordered.Add(poolId);
            }
        }

        ordered.Sort();

        int maxSlots = ordered.Count;
        if (maxSlots <= 0)
        {
            return;
        }

        var sb = new StringBuilder(capacity: 1024);
        sb.AppendLine("namespace Derp.UI;");
        sb.AppendLine();
        sb.AppendLine("internal static partial class UiComponentSlotMap");
        sb.AppendLine("{");
        sb.Append("    public const int MaxSlots = ").Append(maxSlots).AppendLine(";");
        sb.AppendLine();
        sb.AppendLine("    public static int GetSlotIndex(ushort componentKind)");
        sb.AppendLine("    {");
        sb.AppendLine("        return componentKind switch");
        sb.AppendLine("        {");

        for (int i = 0; i < ordered.Count; i++)
        {
            int poolId = ordered[i];
            sb.Append("            ").Append(poolId).Append(" => ").Append(i).AppendLine(",");
        }

        sb.AppendLine("            _ => -1");
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource("UiComponentSlotMap.g.cs", sb.ToString());
    }

    private static void EmitNames(SourceProductionContext context, ImmutableArray<ComponentInfo> infos)
    {
        if (infos.IsDefaultOrEmpty)
        {
            return;
        }

        var byId = new Dictionary<int, string>(capacity: infos.Length);
        for (int i = 0; i < infos.Length; i++)
        {
            ComponentInfo info = infos[i];
            if (info.PoolId <= 0)
            {
                continue;
            }

            if (!byId.ContainsKey(info.PoolId))
            {
                byId.Add(info.PoolId, info.DisplayName ?? string.Empty);
            }
        }

        if (byId.Count == 0)
        {
            return;
        }

        var orderedIds = new List<int>(byId.Keys);
        orderedIds.Sort();

        var sb = new StringBuilder(capacity: 1024);
        sb.AppendLine("namespace Derp.UI;");
        sb.AppendLine();
        sb.AppendLine("internal static class UiComponentKindNames");
        sb.AppendLine("{");
        sb.AppendLine("    public static string GetName(ushort componentKind)");
        sb.AppendLine("    {");
        sb.AppendLine("        return componentKind switch");
        sb.AppendLine("        {");

        for (int i = 0; i < orderedIds.Count; i++)
        {
            int id = orderedIds[i];
            string name = byId[id];
            sb.Append("            ").Append(id).Append(" => \"").Append(Escape(name)).AppendLine("\",");
        }

        sb.AppendLine("            _ => \"Component\"");
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource("UiComponentKindNames.g.cs", sb.ToString());
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private readonly struct ComponentInfo
    {
        public readonly int PoolId;
        public readonly string DisplayName;

        public ComponentInfo(int poolId, string displayName)
        {
            PoolId = poolId;
            DisplayName = displayName;
        }
    }
}
