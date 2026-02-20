using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DerpUi.Ecs.Generator;

[Generator]
public sealed class PropertyGizmoDispatchGenerator : IIncrementalGenerator
{
    private const string PropertyGizmoAttributeMetadataName = "Property.PropertyGizmoAttribute";
    private const string ArrayAttributeMetadataName = "Property.ArrayAttribute";
    private const int MaxArrayLength = 64;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<GizmoTarget> targets = context.SyntaxProvider.ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: PropertyGizmoAttributeMetadataName,
                predicate: static (node, _) => node is VariableDeclaratorSyntax,
                transform: static (syntaxContext, _) => TryGetTarget(syntaxContext))
            .Where(static target => target.IsValid);

        IncrementalValueProvider<ImmutableArray<GizmoTarget>> allTargets = targets.Collect();

        context.RegisterSourceOutput(allTargets, static (sourceContext, collected) =>
        {
            Emit(sourceContext, collected);
        });
    }

    private static GizmoTarget TryGetTarget(GeneratorAttributeSyntaxContext syntaxContext)
    {
        if (syntaxContext.TargetSymbol is not IFieldSymbol fieldSymbol)
        {
            return default;
        }

        if (fieldSymbol.ContainingType is not INamedTypeSymbol componentSymbol)
        {
            return default;
        }

        INamedTypeSymbol? gizmoType = null;
        byte triggers = 0;
        foreach (AttributeData attributeData in syntaxContext.Attributes)
        {
            if (attributeData.AttributeClass == null)
            {
                continue;
            }

            if (attributeData.AttributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != "global::" + PropertyGizmoAttributeMetadataName)
            {
                continue;
            }

            if (attributeData.ConstructorArguments.Length >= 1 &&
                attributeData.ConstructorArguments[0].Kind == TypedConstantKind.Type &&
                attributeData.ConstructorArguments[0].Value is INamedTypeSymbol gizmoTypeSymbol)
            {
                gizmoType = gizmoTypeSymbol;
            }

            if (attributeData.ConstructorArguments.Length >= 2 &&
                attributeData.ConstructorArguments[1].Kind == TypedConstantKind.Enum &&
                attributeData.ConstructorArguments[1].Value is byte triggersByte)
            {
                triggers = triggersByte;
            }
            else if (attributeData.ConstructorArguments.Length >= 2 &&
                     attributeData.ConstructorArguments[1].Kind == TypedConstantKind.Enum &&
                     attributeData.ConstructorArguments[1].Value is int triggersInt)
            {
                triggers = unchecked((byte)triggersInt);
            }

            break;
        }

        if (gizmoType == null)
        {
            return default;
        }

        int arrayLength = 0;
        foreach (AttributeData attributeData in fieldSymbol.GetAttributes())
        {
            if (attributeData.AttributeClass == null)
            {
                continue;
            }

            if (attributeData.AttributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != "global::" + ArrayAttributeMetadataName)
            {
                continue;
            }

            if (attributeData.ConstructorArguments.Length == 1 &&
                attributeData.ConstructorArguments[0].Kind == TypedConstantKind.Primitive &&
                attributeData.ConstructorArguments[0].Value is int len)
            {
                arrayLength = len;
            }

            break;
        }

        if (arrayLength < 0 || arrayLength > MaxArrayLength)
        {
            arrayLength = 0;
        }

        string componentTypeName = componentSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string gizmoTypeName = gizmoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return new GizmoTarget(componentTypeName, fieldSymbol.Name, gizmoTypeName, triggers, arrayLength);
    }

    private static void Emit(SourceProductionContext context, ImmutableArray<GizmoTarget> targets)
    {
        var expanded = new List<GizmoCase>(targets.IsDefault ? 0 : targets.Length);
        if (!targets.IsDefaultOrEmpty)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                GizmoTarget target = targets[i];
                if (!target.IsValid)
                {
                    continue;
                }

                if (target.ArrayLength <= 0)
                {
                    string path = target.ComponentTypeName + "." + target.FieldName;
                    expanded.Add(new GizmoCase(ComputeFnv1a(path), target.GizmoTypeName, target.Triggers));
                    continue;
                }

                for (int index = 0; index < target.ArrayLength; index++)
                {
                    string path = target.ComponentTypeName + "." + target.FieldName + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                    expanded.Add(new GizmoCase(ComputeFnv1a(path), target.GizmoTypeName, target.Triggers));
                }
            }
        }

        expanded.Sort(static (left, right) => left.PropertyId.CompareTo(right.PropertyId));

        EmitInspectorReporter(context, expanded);
        EmitWorkspaceDispatcher(context, expanded);
    }

    private static void EmitInspectorReporter(SourceProductionContext context, List<GizmoCase> cases)
    {
        var sb = new StringBuilder(capacity: 4096);
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using Property.Runtime;");
        sb.AppendLine();
        sb.AppendLine("namespace Derp.UI;");
        sb.AppendLine();
        sb.AppendLine("internal sealed partial class PropertyInspector");
        sb.AppendLine("{");
        sb.AppendLine("    private void ReportGeneratedGizmoSingle(in PropertyUiItem item, PropertySlot slot, bool hovered, bool active)");
        sb.AppendLine("    {");
        sb.AppendLine("        if ((_gizmoTargetEntity.IsNull) || (!hovered && !active))");
        sb.AppendLine("        {");
        sb.AppendLine("            return;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        switch (slot.PropertyId)");
        sb.AppendLine("        {");

        for (int i = 0; i < cases.Count; i++)
        {
            GizmoCase c = cases[i];
            sb.AppendLine("            case " + c.PropertyId.ToString(CultureInfo.InvariantCulture) + "UL:");
            sb.AppendLine("            {");
            if ((c.Triggers & 1) != 0)
            {
                sb.AppendLine("                if (hovered)");
                sb.AppendLine("                {");
                sb.AppendLine("                    _commands.ReportPropertyGizmo(_gizmoTargetEntity, item.WidgetId, slot, PropertyGizmoPhase.Hover, payload0: 0, payload1: 0);");
                sb.AppendLine("                }");
            }
            if ((c.Triggers & 2) != 0)
            {
                sb.AppendLine("                if (active)");
                sb.AppendLine("                {");
                sb.AppendLine("                    _commands.ReportPropertyGizmo(_gizmoTargetEntity, item.WidgetId, slot, PropertyGizmoPhase.Active, payload0: 0, payload1: 0);");
                sb.AppendLine("                }");
            }
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
        }

        sb.AppendLine("            default:");
        sb.AppendLine("                return;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource("PropertyInspector.Gizmos.g.cs", sb.ToString());
    }

    private static void EmitWorkspaceDispatcher(SourceProductionContext context, List<GizmoCase> cases)
    {
        var sb = new StringBuilder(capacity: 4096);
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Numerics;");
        sb.AppendLine("using DerpLib.ImGui.Input;");
        sb.AppendLine();
        sb.AppendLine("namespace Derp.UI;");
        sb.AppendLine();
        sb.AppendLine("public sealed partial class UiWorkspace");
        sb.AppendLine("{");
        sb.AppendLine("    private void DrawGeneratedPropertyGizmo(int frameId, Vector2 canvasOrigin, in PropertyGizmoRequest request)");
        sb.AppendLine("    {");
        sb.AppendLine("        switch (request.Slot.PropertyId)");
        sb.AppendLine("        {");

        for (int i = 0; i < cases.Count; i++)
        {
            GizmoCase c = cases[i];
            sb.AppendLine("            case " + c.PropertyId.ToString(CultureInfo.InvariantCulture) + "UL:");
            sb.AppendLine("            {");
            sb.AppendLine("                if (request.Phase == PropertyGizmoPhase.Hover)");
            sb.AppendLine("                {");
            sb.AppendLine("                    " + c.GizmoTypeName + ".DrawHover(this, frameId, canvasOrigin, request);");
            sb.AppendLine("                    return;");
            sb.AppendLine("                }");
            sb.AppendLine();
            sb.AppendLine("                " + c.GizmoTypeName + ".DrawActive(this, frameId, canvasOrigin, request);");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
        }

        sb.AppendLine("            default:");
        sb.AppendLine("                return;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private bool HandleGeneratedPropertyGizmoInput(in ImInput input, Vector2 canvasOrigin, Vector2 mouseCanvas, in PropertyGizmoRequest request)");
        sb.AppendLine("    {");
        sb.AppendLine("        switch (request.Slot.PropertyId)");
        sb.AppendLine("        {");

        for (int i = 0; i < cases.Count; i++)
        {
            GizmoCase c = cases[i];
            sb.AppendLine("            case " + c.PropertyId.ToString(CultureInfo.InvariantCulture) + "UL:");
            sb.AppendLine("                return " + c.GizmoTypeName + ".HandleInput(this, input, canvasOrigin, mouseCanvas, request);");
        }

        sb.AppendLine("            default:");
        sb.AppendLine("                return false;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource("UiWorkspace.PropertyGizmos.g.cs", sb.ToString());
    }

    private static ulong ComputeFnv1a(string text)
    {
        const ulong fnvOffset = 14695981039346656037UL;
        const ulong fnvPrime = 1099511628211UL;

        ulong hash = fnvOffset;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c <= 0x7F)
            {
                hash ^= (byte)c;
                hash *= fnvPrime;
                continue;
            }

            // UTF8 encode codepoint as bytes without allocations.
            if (c < 0x800)
            {
                hash ^= (byte)(0xC0 | (c >> 6));
                hash *= fnvPrime;
                hash ^= (byte)(0x80 | (c & 0x3F));
                hash *= fnvPrime;
                continue;
            }

            hash ^= (byte)(0xE0 | (c >> 12));
            hash *= fnvPrime;
            hash ^= (byte)(0x80 | ((c >> 6) & 0x3F));
            hash *= fnvPrime;
            hash ^= (byte)(0x80 | (c & 0x3F));
            hash *= fnvPrime;
        }

        return hash;
    }

    private readonly struct GizmoTarget
    {
        public readonly string ComponentTypeName;
        public readonly string FieldName;
        public readonly string GizmoTypeName;
        public readonly byte Triggers;
        public readonly int ArrayLength;
        public bool IsValid => !string.IsNullOrEmpty(ComponentTypeName) && !string.IsNullOrEmpty(FieldName) && !string.IsNullOrEmpty(GizmoTypeName);

        public GizmoTarget(string componentTypeName, string fieldName, string gizmoTypeName, byte triggers, int arrayLength)
        {
            ComponentTypeName = componentTypeName;
            FieldName = fieldName;
            GizmoTypeName = gizmoTypeName;
            Triggers = triggers;
            ArrayLength = arrayLength;
        }
    }

    private readonly struct GizmoCase
    {
        public readonly ulong PropertyId;
        public readonly string GizmoTypeName;
        public readonly byte Triggers;

        public GizmoCase(ulong propertyId, string gizmoTypeName, byte triggers)
        {
            PropertyId = propertyId;
            GizmoTypeName = gizmoTypeName;
            Triggers = triggers;
        }
    }
}

